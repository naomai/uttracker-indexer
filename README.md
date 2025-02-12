# UTTracker Indexer
Server data collector for [Unreal Tournament Stats Tracker](https://github.com/naomai/uttracker-web).

Periodically grabs statistics from all game servers online.

## Features
- Get server info, players list and match stats
- Fetch server lists from other master servers
- Verify servers for fake players
- [XServerQuery](https://ut99.org/ut99.org/viewtopic.php?t=6061) support
- Optional: Hosting a UT master server, providing list of all online servers

## Requirements
- .NET 8.0 Runtime (
        [Linux](https://learn.microsoft.com/en-us/dotnet/core/install/linux) 
        / [Windows](https://learn.microsoft.com/en-us/dotnet/core/install/windows)
    )
- MySQL/MariaDB database - same, as for [Web component](https://github.com/naomai/uttracker-web)

## Build

```sh
# Linux x64:
dotnet build src/Indexer.vbproj --configuration Release -r:linux-x64 --no-self-contained

# Windows x64:
dotnet build src/Indexer.vbproj --configuration Release -r:win-x64 --no-self-contained
```

The build products are in directory **/src/bin/Release/net8.0/**

## Setup (standalone)
Launch Indexer for the first time to create configuration file:
```sh
./Indexer
```
```log
[14:48:12] Loading config file from: /home/app/Indexer.ini
Unhandled exception. System.Exception: Please configure the scanner first
```

Edit `Indexer.ini` file, providing database info. This should be the same
database, as used by Web compoment. To **reduce security risks**, create 
dedicated DB account for Indexer, limited to SELECT, INSERT, UPDATE and DELETE
on the database tables.

```ini
[Database]
MySQLHost=localhost
MySQLUser=uttIndexer
MySQLPass=12345
MySQLDB=uttracker
```

The indexer can now be launched, and will become a continuously running 
process:
```sh
./Indexer
```

## Setup (docker)
Database connection can also be configured by setting environment variables:
```sh
export DB_HOST=localhost
export DB_USERNAME=uttIndexer
export DB_PASSWORD=12345
export DB_DATABASE=uttracker
```