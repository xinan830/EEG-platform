[CmdletBinding(SupportsShouldProcess = $true)]
param()

$ErrorActionPreference = 'Stop'

$localAppData = [Environment]::GetFolderPath(
    [Environment+SpecialFolder]::LocalApplicationData)
if ([string]::IsNullOrWhiteSpace($localAppData)) {
    throw '无法定位 LOCALAPPDATA，未执行清理。'
}

$cacheRoots = @(
    (Join-Path $localAppData 'BrainPlatform\review-cache'),
    (Join-Path $localAppData 'BrainPlatform\review-display-cache')
)

foreach ($cacheRoot in $cacheRoots) {
    if (-not (Test-Path -LiteralPath $cacheRoot -PathType Container)) {
        Write-Host "不存在，跳过：$cacheRoot"
        continue
    }

    if ($PSCmdlet.ShouldProcess($cacheRoot, '删除可重建缓存')) {
        Remove-Item -LiteralPath $cacheRoot -Recurse -Force
        Write-Host "已清除：$cacheRoot"
    }
}

Write-Host '完成。未触及录音、分析结果、数据库、日志和用户配置。'
