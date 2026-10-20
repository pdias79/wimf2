param([Parameter(Mandatory=$true)][string]$Output)
Add-Type -AssemblyName System.Drawing
$size = 64
$bmp = [System.Drawing.Bitmap]::new($size, $size)
try {
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  try {
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::FromArgb(21,91,188))
    $white = [System.Drawing.Pen]::new([System.Drawing.Color]::White, 5)
    $cyan = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(70,166,217), 3)
    $gold = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(255,194,71), 9)
    try {
      $g.FillEllipse([System.Drawing.Brushes]::AliceBlue, 12, 11, 30, 30)
      $g.DrawEllipse($white, 12, 11, 30, 30)
      $g.DrawEllipse($cyan, 18, 17, 18, 18)
      $g.DrawLine($gold, 39, 39, 53, 53)
    } finally { $white.Dispose(); $cyan.Dispose(); $gold.Dispose() }
  } finally { $g.Dispose() }
  $handle = $bmp.GetHicon()
  try {
    $icon = [System.Drawing.Icon]::FromHandle($handle)
    $directory = [System.IO.Path]::GetDirectoryName($Output)
    [System.IO.Directory]::CreateDirectory($directory) | Out-Null
    $stream = [System.IO.File]::Create($Output)
    try { $icon.Save($stream) } finally { $stream.Dispose() }
  } finally {
    # Windows owns the HICON returned by GetHicon.
    Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; public static class NativeIcon { [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr handle); }' -ErrorAction SilentlyContinue
    [NativeIcon]::DestroyIcon($handle) | Out-Null
  }
} finally { $bmp.Dispose() }
