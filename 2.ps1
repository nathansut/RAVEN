# Define the path to the Variables.txt file
$variablesFilePath = "$PSScriptRoot\Variables.txt"

# Initialize variables
$imgFile = ""

# Read the Variables.txt file and parse variables
Get-Content -Path $variablesFilePath | ForEach-Object {
    if ($_ -match "^ImgFile=(.*)$") {
        $imgFile = $matches[1]
    }
    # You can parse other variables here if needed
}

# Define the path to the IrfanView executable
$irfanViewPath = "C:\Program Files\IrfanView\i_view64.exe"

# Launch IrfanView with the specified image
Start-Process -FilePath $irfanViewPath -ArgumentList $imgFile
