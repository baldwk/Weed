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
    },
    @{
        Name = "ch_ppocr_mobile_v2.0_cls_infer.onnx"
        Url = "https://www.modelscope.cn/models/RapidAI/RapidOCR/resolve/v3.6.0/onnx/PP-OCRv4/cls/ch_ppocr_mobile_v2.0_cls_infer.onnx"
    },
    @{
        Name = "ch_PP-OCRv5_rec_mobile_infer.onnx"
        Url = "https://www.modelscope.cn/models/RapidAI/RapidOCR/resolve/v3.6.0/onnx/PP-OCRv5/rec/ch_PP-OCRv5_rec_mobile_infer.onnx"
    },
    @{
        Name = "ppocrv5_dict.txt"
        Url = "https://www.modelscope.cn/models/RapidAI/RapidOCR/resolve/v3.6.0/paddle/PP-OCRv5/rec/ch_PP-OCRv5_rec_mobile_infer/ppocrv5_dict.txt"
    }
)

foreach ($file in $files) {
    $target = Join-Path $Destination $file.Name
    if (Test-Path $target) {
        Write-Host "Already exists: $target"
        continue
    }

    Write-Host "Downloading $($file.Name)..."
    Invoke-WebRequest -UseBasicParsing -Uri $file.Url -OutFile $target
}

Get-ChildItem $Destination | Select-Object Name, Length, @{Name = "MiB"; Expression = { [math]::Round($_.Length / 1MB, 2) } }
