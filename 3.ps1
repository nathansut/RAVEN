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

# Define the output file path
$outputFilePath = "c:\Client\phreview.cnt"

# Write the $imgFile variable to the output file, overwriting any existing content
Set-Content -Path $outputFilePath -Value $imgFile


# Define the output file path
$casjFilePath = "c:\casj\phreviewsplit.exe"

Start-Process -FilePath $casjFilePath 