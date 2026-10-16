param([string]$Database = "SadrSchemaCheck")
$cs = "Data Source=.\SQLExpress;Initial Catalog=$Database;User ID=sa;Password=123asd!@#ASD;TrustServerCertificate=True;Encrypt=True"
$con = New-Object System.Data.SqlClient.SqlConnection($cs)
$con.Open()
$cmd = $con.CreateCommand()
$cmd.CommandText = "SELECT OBJECT_NAME(i.object_id) AS tbl, i.name, i.is_unique, i.has_filter, i.filter_definition FROM sys.indexes i WHERE i.name LIKE '%_Code' ORDER BY tbl, name"
$r = $cmd.ExecuteReader()
while ($r.Read()) { Write-Output ("{0} | {1} | unique={2} | filter={3} | {4}" -f $r['tbl'], $r['name'], $r['is_unique'], $r['has_filter'], $r['filter_definition']) }
$r.Close()
$con.Close()
Write-Output "DONE"
