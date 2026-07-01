$connString = "Server=DESKTOP-NQH197;Database=WorkspaceHub;Trusted_Connection=True;TrustServerCertificate=True"
$connection = New-Object System.Data.SqlClient.SqlConnection($connString)
$connection.Open()
$command = $connection.CreateCommand()

# Query count grouped by type
$command.CommandText = "SELECT Type, COUNT(*) as Count FROM Items WHERE UserId = 'F587F642-99AF-472D-8674-84AAB162DE87' GROUP BY Type"
$reader = $command.ExecuteReader()
while ($reader.Read()) {
    Write-Host "Type: $($reader['Type']) | Count: $($reader['Count'])"
}
$reader.Close()

# Query first 5 items of each type
$types = @("Email", "Event", "File", "Note", "Ticket")
foreach ($t in $types) {
    $command.CommandText = "SELECT TOP 3 Title, Status FROM Items WHERE UserId = 'F587F642-99AF-472D-8674-84AAB162DE87' AND Type = '$t'"
    $reader = $command.ExecuteReader()
    Write-Host "--- $t ---"
    while ($reader.Read()) {
        Write-Host "Title: $($reader['Title']) | Status: $($reader['Status'])"
    }
    $reader.Close()
}

$connection.Close()
