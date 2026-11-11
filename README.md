# Folder sync

A small C# console program that keeps a replica folder identical to a source folder.
Synchronization is one-way and runs on a fixed interval. Every folder creation, file copy,
file update, and removal is written to the console and appended to a log file.

This is an implementation of the Veeam Software folder-synchronization test task.
It uses the .NET base class library only. There is no third-party sync package.

## Requirements

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (the current LTS)

## Build

```bash
dotnet build FolderSync.sln
```

## Run

```bash
dotnet run --project src/FolderSync -- \
  --source /path/to/source \
  --replica /path/to/replica \
  --interval 60 \
  --log /path/to/logs/sync.log
```

The same arguments work on the published binary:

```bash
dotnet publish src/FolderSync -c Release -o out
./out/FolderSync --source ./source --replica ./replica --interval 60 --log ./logs/sync.log
```

The process keeps running. The first sync starts immediately, then repeats every `--interval` seconds.
Press Ctrl+C once to stop after the current file. Press it a second time to terminate immediately.
`SIGTERM` also requests a clean stop.

## Arguments

| Option | Short | Meaning |
| --- | --- | --- |
| `--source <path>` | `-s` | Folder to copy from. Must already exist and must be a real folder, not a symbolic link. |
| `--replica <path>` | `-r` | Folder to update. Created if it is missing. Must not be a symbolic link. |
| `--interval <seconds>` | `-i` | How often to sync. A whole number of seconds greater than 0. |
| `--log <path>` | `-l` | Log file to append to. Parent folders are created. |
| `--help` | `-h` | Show usage and exit. |

Values can also be written as `--interval=60`. Paths may be relative; they are stored as absolute paths.

The program rejects:

- a missing source, or a source that is a file
- an interval that is not a whole number of seconds greater than 0
- source and replica that are the same folder, or where one is inside the other
- a log file placed inside the source or the replica (it would be copied forever, or deleted)
- a log path that points at a folder

Exit code `0` means the arguments were accepted and the process stopped cleanly.
Exit code `1` means the arguments were rejected or the log file could not be opened.
Invalid arguments print the error and the usage text to standard error.

## What a sync does

After each pass, the replica tree matches the source tree:

- files that exist only in the source are copied
- files whose contents changed are overwritten
- files and folders that exist only in the replica are deleted
- folders that exist only in the source are created, including empty ones
- a file that occupies the path of a source folder (or the other way around) is replaced

Comparison is deliberately cheap:

1. Different size means the file changed, so it is copied. No hash is computed.
2. Same size and the same UTC last-write time means the file is left alone.
3. Same size but a different last-write time falls back to an MD5 checksum (`System.Security.Cryptography.MD5`).
   Identical bytes only get their timestamp aligned. Different bytes are copied.

The copy keeps the source last-write time, so the next pass can trust the cheap check.
MD5 is a content checksum here, not a security control. Files are hashed from a stream, not loaded all at once.

A failure on one entry (access denied, file in use, and similar IO errors) is logged and the pass continues.
The next interval tries that entry again. An unavailable source is logged the same way; the process keeps running.

Symbolic links are not followed and are not recreated. A directory link that points at a parent would
otherwise recurse forever, and deleting through a link could remove files outside the replica.
Link entries are logged as skipped or removed. The link target is left untouched.

## Log output

Each line is timestamped and written to the console and the log file:

```text
2026-11-10 23:45:01.123 +00:00 [INFO] Folder sync is running. Source: /data/source Replica: /data/replica Interval: 60s Log: /var/log/folder-sync.log
2026-11-10 23:45:01.130 +00:00 [INFO] Sync started. Source: /data/source Replica: /data/replica
2026-11-10 23:45:01.141 +00:00 [INFO] Created folder: /data/replica/empty
2026-11-10 23:45:01.155 +00:00 [INFO] Copied file: /data/source/notes.txt -> /data/replica/notes.txt
2026-11-10 23:45:01.170 +00:00 [INFO] Updated file: /data/source/report.csv -> /data/replica/report.csv
2026-11-10 23:45:01.188 +00:00 [INFO] Removed file: /data/replica/old.txt
2026-11-10 23:45:01.201 +00:00 [INFO] Removed folder: /data/replica/obsolete
2026-11-10 23:45:01.210 +00:00 [ERROR] Failed to sync file '/data/source/locked.bin' to '/data/replica/locked.bin': Access to the path is denied.
2026-11-10 23:45:01.220 +00:00 [INFO] Sync finished. Copied: 1, updated: 1, folders created: 1, files removed: 1, folders removed: 1, failed: 1.
2026-11-10 23:46:01.000 +00:00 [INFO] Stop requested.
2026-11-10 23:46:01.001 +00:00 [INFO] Folder sync stopped.
```

The log file is UTF-8. The logger creates parent folders and appends, so restarting the process keeps the history.

## Design

The program is a console loop, not a generic host. There are four settings and one periodic job, so `PeriodicTimer` is enough and the control flow stays visible.

`src/FolderSync` is split into the pieces a reviewer can read separately:

- `Program` owns the interval, Ctrl+C, and `SIGTERM`
- `ArgumentParser` is a small hand-rolled parser and validates the paths before the first sync
- `FolderSynchronizer` does one pass and returns counts
- `SyncLogger` writes one timestamped line to the console and the log file

Passes do not overlap. The loop awaits a pass before it waits for the next tick, and `FolderSynchronizer` ignores a second call that arrives while a pass is still running. That matters when a sync takes longer than the interval: the next one starts after the current one finishes, instead of running beside it.

Cancellation is checked between files and folders. A single file copy is not aborted halfway through.

Directory last-write times are not copied. The replica matches the source by entries and file bytes. A file with the same size and the same last-write time is treated as unchanged, even if something rewrote the bytes without touching either value. That tradeoff keeps the common pass cheap; the MD5 check covers the usual "touched, or copied from somewhere else" case.

## Tests

```bash
dotnet test FolderSync.sln
```

The xUnit project uses temporary directories. It covers:

- creating files, updating them, and deleting files and folders that are not in the source
- nested folders and empty directories
- the MD5 fallback and timestamp alignment
- a second pass that does nothing when the tree is already in sync
- per-file access errors, so one failure does not stop the pass
- symbolic links, overlapping calls, and cancellation
- argument validation (missing values, interval, missing source, nesting, log location)

## Layout

```text
FolderSync.sln
src/FolderSync/          Console program
tests/FolderSync.Tests/  xUnit tests
```
