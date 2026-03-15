---
name: build-raven
description: Build RAVEN, commit, push, zip, and deploy to ns1-wsl and I:\jobs\imaging_tools\raven\
disable-model-invocation: true
allowed-tools: Bash, Read, Grep, Glob
---

# Build and Deploy RAVEN

Full build-and-deploy pipeline for RAVEN. Runs all steps sequentially, reporting progress.
If a deploy target is unavailable, warn but continue with the remaining steps.

## Step 0: Git Commit and Push

1. Run `cd /mnt/c/dev/RAVEN && git status` to see changes
2. Stage all modified/new files: `git add -A`
3. If `$ARGUMENTS` is provided, use it as the commit message. Otherwise, use a **Sonnet agent** to scan `git diff` and `git diff --stat`, then generate a brief, specific commit message covering the key functional changes (not just file names). Present the message to the user for confirmation before committing.
4. Commit and push to origin:
   ```
   git commit -m "<message>"
   git push origin
   ```
5. If push fails, warn but continue

## Step 1: Build (Debug)

```
cd /mnt/c/dev/RAVEN && "/mnt/c/Program Files/dotnet/dotnet.exe" build --configuration Debug --nologo
```

- Check output for `Error(s)` — if any **code errors** (CS*), STOP and report them
- MSB3027 (locked exe) means RAVEN is running — tell the user to close it and retry
- Warnings are OK, ignore them

## Step 2: Zip the Debug output

```
powershell.exe -Command "Compress-Archive -Path 'C:\dev\RAVEN\bin\Debug\net8.0-windows\*' -DestinationPath 'C:\temp\RAVEN_build.zip' -Force"
```

Verify the zip was created: `ls -lh /mnt/c/temp/RAVEN_build.zip`

## Step 3a: Deploy to ns1-wsl

ns1-wsl is on Tailscale. Connection details:
- IP: Find dynamically via `tailscale status | grep ns1-wsl | awk '{print $1}'`
- Login: `index` / password: `index`
- Use sshpass: `sshpass -p index scp -o StrictHostKeyChecking=no <file> index@<ip>:<dest>`

Steps:
1. Get the Tailscale IP for ns1-wsl
2. SCP the zip to ns1-wsl: `sshpass -p index scp -o StrictHostKeyChecking=no /mnt/c/temp/RAVEN_build.zip index@<ip>:~/RAVEN_build.zip`
3. SSH in and extract: `sshpass -p index ssh -o StrictHostKeyChecking=no index@<ip> "cd ~ && unzip -o RAVEN_build.zip -d ~/RAVEN/"`

If SSH requires Tailscale browser approval, print the approval URL and warn the user.
If ns1-wsl is unreachable, warn but continue.

## Step 3b: Deploy to I:\jobs\imaging_tools\raven\

I: drive is a network share (`\\pagrape\scanning`). It may not always be available.

```
powershell.exe -Command "if (Test-Path 'I:\') { Expand-Archive -Path 'C:\temp\RAVEN_build.zip' -DestinationPath 'I:\jobs\imaging_tools\raven' -Force } else { Write-Host 'WARNING: I: drive not available - skipping' }"
```

IMPORTANT: Do NOT delete any existing files on I:\jobs\imaging_tools\raven\ — only overwrite with newer versions from the zip. The `-Force` flag on `Expand-Archive` overwrites existing files but preserves anything not in the zip.

## Summary

After all steps, print a summary:
- Git: committed + pushed (or error)
- Build: success (or errors)
- Zip: size
- ns1-wsl: deployed (or skipped)
- I: drive: deployed (or skipped)
