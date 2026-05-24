rem wincc02, localhost dbs
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=.;Database=dkzx_mis_mssql;Trusted_Connection=true;TrustServerCertificate=True;Encrypt=False;Connection Timeout=60;MultipleActiveResultSets=False;"   

ren not localhost dbs
rem dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=.;Database=dkzx_mis_mssql;Trusted_Connection=true;TrustServerCertificate=True;Encrypt=False;Connection Timeout=60;MultipleActiveResultSets=False;"   