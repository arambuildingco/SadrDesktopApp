param([string]$Database = "SadrTest")
$cs = "Data Source=.\SQLExpress;Initial Catalog=$Database;User ID=sa;Password=123asd!@#ASD;TrustServerCertificate=True;Encrypt=True"
$tables = @('People','Companies','Products','Brands','Banks','Currencies','WareHouses','CustomersProviders','AccountGroups','GeneralAccounts','SubSidiaryAccounts','DetailAccounts','Cashes')
$con = New-Object System.Data.SqlClient.SqlConnection($cs)
$con.Open()
foreach ($t in $tables) {
    $cmd = $con.CreateCommand()
    $cmd.CommandText = "SELECT i.name, i.is_unique, i.has_filter FROM sys.indexes i WHERE i.object_id = OBJECT_ID('dbo.$t') AND i.name LIKE '%_Code'"
    $r = $cmd.ExecuteReader()
    while ($r.Read()) { Write-Output ("{0}: {1} unique={2} filter={3}" -f $t, $r['name'], $r['is_unique'], $r['has_filter']) }
    $r.Close()
}
# duplicate-code stats (would block index creation if non-empty dups exist)
foreach ($t in $tables) {
    $cmd = $con.CreateCommand()
    $cmd.CommandText = "SELECT COUNT(*) AS c FROM dbo.$t WHERE Deleted = 0 AND Code IS NOT NULL AND Code <> '' GROUP BY Code HAVING COUNT(*) > 1"
    $n = $cmd.ExecuteScalar()
    if ($n -ne $null -and [int]$n -gt 0) { Write-Output ("DUPCODES in {0}" -f $t) }
}
$con.Close()
Write-Output "DONE"
