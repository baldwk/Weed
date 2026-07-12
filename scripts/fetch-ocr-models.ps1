param(
    [string]$Destination = ""
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
if ([string]::IsNullOrWhiteSpace($Destination)) {
    $Destination = Join-Path $repoRoot "External Plugins\Weed.Plugins.Ocr\models"
}

New-Item -ItemType Directory -Force -Path $Destination | Out-Null

$files = @(
    @{
        Name = "ch_PP-OCRv5_mobile_det.onnx"
        Url = "https://www.modelscope.cn/models/RapidAI/RapidOCR/resolve/v3.6.0/onnx/PP-OCRv5/det/ch_PP-OCRv5_mobile_det.onnx"
        Sha256 = "4d97c44a20d30a81aad087d6a396b08f786c4635742afc391f6621f5c6ae78ae"
    },
    @{
        Name = "ch_ppocr_mobile_v2.0_cls_infer.onnx"
        Url = "https://www.modelscope.cn/models/RapidAI/RapidOCR/resolve/v3.6.0/onnx/PP-OCRv4/cls/ch_ppocr_mobile_v2.0_cls_infer.onnx"
        Sha256 = "e47acedf663230f8863ff1ab0e64dd2d82b838fceb5957146dab185a89d6215c"
    },
    @{
        Name = "ch_PP-OCRv5_rec_mobile_infer.onnx"
        Url = "https://www.modelscope.cn/models/RapidAI/RapidOCR/resolve/v3.6.0/onnx/PP-OCRv5/rec/ch_PP-OCRv5_rec_mobile_infer.onnx"
        Sha256 = "5825fc7ebf84ae7a412be049820b4d86d77620f204a041697b0494669b1742c5"
    },
    @{
        Name = "ppocrv5_dict.txt"
        Url = "https://www.modelscope.cn/models/RapidAI/RapidOCR/resolve/v3.6.0/paddle/PP-OCRv5/rec/ch_PP-OCRv5_rec_mobile_infer/ppocrv5_dict.txt"
        Sha256 = "d1979e9f794c464c0d2e0b70a7fe14dd978e9dc644c0e71f14158cdf8342af1b"
    }
)

foreach ($file in $files) {
    $target = Join-Path $Destination $file.Name
    if (Test-Path $target) {
        $existingHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $target).Hash.ToLowerInvariant()
        if ($existingHash -eq $file.Sha256) {
            Write-Host "Verified existing model: $target"
            continue
        }

        Remove-Item -LiteralPath $target -Force
    }

    Write-Host "Downloading $($file.Name)..."
    $download = "$target.download"
    try {
        Invoke-WebRequest -UseBasicParsing -Uri $file.Url -OutFile $download
        $downloadHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $download).Hash.ToLowerInvariant()
        if ($downloadHash -ne $file.Sha256) {
            throw "Model checksum mismatch for $($file.Name). Expected $($file.Sha256), got $downloadHash."
        }

        Move-Item -LiteralPath $download -Destination $target -Force
    }
    finally {
        if (Test-Path -LiteralPath $download) {
            Remove-Item -LiteralPath $download -Force
        }
    }
}

Get-ChildItem $Destination | Select-Object Name, Length, @{Name = "MiB"; Expression = { [math]::Round($_.Length / 1MB, 2) } }
