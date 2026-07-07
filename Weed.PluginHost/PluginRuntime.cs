using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Weed.Abstractions;
using Weed.Core;
using IQueryProvider = Weed.Abstractions.IQueryProvider;

namespace Weed.PluginHost;

public sealed class PluginRuntime : IAsyncDisposable
{
    private readonly IWeedHost _host;
    private readonly IWeedLogger _logger;
    private readonly List<PluginLoadContext> _loadContexts = [];
    private readonly List<LoadedPlugin> _plugins = [];

    public PluginRuntime(IWeedHost host, IWeedLogger logger)
    {
        _host = host;
        _logger = logger;
    }

    public IReadOnlyList<LoadedPlugin> Plugins => _plugins;

    public async ValueTask AddBuiltInAsync(WeedPluginManifest manifest, IWeedPlugin plugin, CancellationToken cancellationToken)
    {
        try
        {
            await plugin.InitializeAsync(_host, cancellationToken);
            _plugins.Add(ToLoadedPlugin(manifest, plugin));
            _logger.Info($"Loaded built-in plugin {manifest.Id}.");
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to load built-in plugin {manifest.Id}.", ex);
        }
    }

    public async ValueTask ScanDirectoryAsync(string pluginsDirectory, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(pluginsDirectory))
        {
            Directory.CreateDirectory(pluginsDirectory);
            return;
        }

        foreach (var manifestPath in Directory.EnumerateFiles(pluginsDirectory, "manifest.json", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var manifest = JsonSerializer.Deserialize<WeedPluginManifest>(
                    await File.ReadAllTextAsync(manifestPath, cancellationToken),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (manifest?.Assembly is null || manifest.EntryType is null)
                {
                    _logger.Warn($"Skipped malformed plugin manifest at {manifestPath}.");
                    continue;
                }

                var pluginDirectory = Path.GetDirectoryName(manifestPath)!;
                var assemblyPath = Path.Combine(pluginDirectory, manifest.Assembly);
                if (!File.Exists(assemblyPath))
                {
                    _logger.Warn($"Plugin assembly not found: {assemblyPath}");
                    continue;
                }

                var context = new PluginLoadContext(pluginDirectory);
                var assembly = context.LoadFromAssemblyPath(assemblyPath);
                var type = assembly.GetType(manifest.EntryType, throwOnError: true);
                if (type is null || !typeof(IWeedPlugin).IsAssignableFrom(type))
                {
                    _logger.Warn($"Plugin entry type does not implement IWeedPlugin: {manifest.EntryType}");
                    continue;
                }

                var plugin = (IWeedPlugin)Activator.CreateInstance(type)!;
                await plugin.InitializeAsync(_host, cancellationToken);
                _plugins.Add(ToLoadedPlugin(manifest, plugin));
                _loadContexts.Add(context);
                _logger.Info($"Loaded external plugin {manifest.Id}.");
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to load plugin manifest at {manifestPath}.", ex);
            }
        }
    }

    public async ValueTask StartResidentsAsync(CancellationToken cancellationToken)
    {
        foreach (var plugin in _plugins.Where(p => p.ResidentPlugin is not null && p.Manifest.Runtime.Resident))
        {
            try
            {
                await plugin.ResidentPlugin!.StartAsync(cancellationToken);
                _logger.Info($"Started resident plugin {plugin.Manifest.Id}.");
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to start resident plugin {plugin.Manifest.Id}.", ex);
            }
        }
    }

    public async ValueTask StopResidentsAsync(CancellationToken cancellationToken)
    {
        foreach (var plugin in _plugins.Where(p => p.ResidentPlugin is not null).Reverse())
        {
            try
            {
                await plugin.ResidentPlugin!.StopAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to stop resident plugin {plugin.Manifest.Id}.", ex);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await StopResidentsAsync(cts.Token);

        foreach (var plugin in _plugins.AsEnumerable().Reverse())
        {
            try
            {
                await plugin.Instance.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to dispose plugin {plugin.Manifest.Id}.", ex);
            }
        }

        foreach (var context in _loadContexts)
        {
            context.Unload();
        }
    }

    private static LoadedPlugin ToLoadedPlugin(WeedPluginManifest manifest, IWeedPlugin plugin) => new(
        manifest,
        plugin,
        plugin as IQueryProvider,
        plugin as ICommandHandler,
        plugin as IResidentPlugin);
}

internal sealed class PluginLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;

    public PluginLoadContext(string pluginPath)
        : base(isCollectible: true)
    {
        _resolver = new AssemblyDependencyResolver(pluginPath);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (assemblyName.Name == typeof(IWeedPlugin).Assembly.GetName().Name)
        {
            return null;
        }

        var assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);
        return assemblyPath is null ? null : LoadFromAssemblyPath(assemblyPath);
    }
}
