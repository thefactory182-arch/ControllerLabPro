$ErrorActionPreference = 'Stop'
$modelPath = Join-Path $PSScriptRoot 'src/ControllerLabPro/Models/yolox_nano.onnx'
$expected = 'c789161ed43c8269fcd4e67c67eeeb4e80c622da2eb296a20bc6007bd18a0b7d'
if (!(Test-Path -LiteralPath $modelPath) -or (Get-FileHash -LiteralPath $modelPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected) {
    Invoke-WebRequest 'https://github.com/Megvii-BaseDetection/YOLOX/releases/download/0.1.1rc0/yolox_nano.onnx' -OutFile $modelPath
}
if ((Get-FileHash -LiteralPath $modelPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected) { throw 'YOLOX model checksum mismatch; refusing to build.' }
Write-Output 'Official YOLOX Nano model verified.'
