#!/usr/bin/env bash
# Read-only probe for DiskGuard v3 design. ASCII labels only (GBK console).
export LC_ALL=C.UTF-8
U=/c/Users/wrl90
L=$U/AppData/Local
R=$U/AppData/Roaming
sz() { [ -e "$1" ] && printf '%8s  %s\n' "$(du -sh "$1" 2>/dev/null | cut -f1)" "$1"; }

echo '== browsers (cache only) =='
for b in "$L/Google/Chrome/User Data" "$L/Microsoft/Edge/User Data"; do
  for p in "$b"/Default "$b"/Profile*; do
    [ -d "$p" ] || continue
    for c in Cache "Code Cache" GPUCache "Service Worker/CacheStorage"; do sz "$p/$c"; done
  done
  sz "$b/ShaderCache"; sz "$b/GrShaderCache"
done

echo '== electron apps: Cache/Code Cache/GPUCache/CachedData/logs under Roaming and Local =='
for base in "$R" "$L"; do
  for app in "$base"/*; do
    [ -d "$app" ] || continue
    for c in Cache "Code Cache" GPUCache CachedData CachedExtensionVSIXs logs "Service Worker/CacheStorage" "Crashpad/completed"; do
      [ -d "$app/$c" ] && sz "$app/$c"
    done
  done
done | sort -rh | head -n 40

echo '== VS Code / Cursor workspaceStorage (history, not default) =='
sz "$R/Code/User/workspaceStorage"; sz "$R/Cursor/User/workspaceStorage"

echo '== package managers =='
sz "$L/npm-cache"; sz "$R/npm-cache"; sz /e/develop/nodejs/node_cache
npm config get cache 2>/dev/null | sed 's/^/npm cache dir: /'
sz "$L/pip/cache"; sz "$L/pip"; sz "$L/Yarn/Cache"; sz "$L/pnpm/store"; sz "$L/pnpm-cache"
sz "$U/.gradle/caches"; sz "$U/.m2/repository"; sz "$U/.nuget/packages"; sz "$L/NuGet/v3-cache"
sz "$U/.cargo/registry"; sz "$U/.cache"; ls -1 "$U/.cache" 2>/dev/null | head -n 20 | sed 's/^/  .cache\//'
for d in "$U/.cache"/*; do sz "$d"; done | sort -rh | head -n 10

echo '== windows =='
sz "$L/CrashDumps"; sz "$L/Microsoft/Windows/WER"; sz /c/ProgramData/Microsoft/Windows/WER
sz "$L/D3DSCache"; sz "$L/NVIDIA/DXCache"; sz "$L/NVIDIA/GLCache"; sz "$L/AMD/DxCache"
sz "$L/Microsoft/Windows/Explorer"
sz /c/Windows/SoftwareDistribution/DeliveryOptimization; sz /c/Windows/ServiceProfiles/NetworkService/AppData/Local/Microsoft/Windows/DeliveryOptimization
sz /c/Windows/Logs; sz /c/Windows/Logs/CBS; sz /c/Windows/Prefetch; sz /c/Windows/Minidump; ls -l /c/Windows/MEMORY.DMP 2>/dev/null
sz "$L/Microsoft/Windows/INetCache"
ls -l /c/hiberfil.sys /c/pagefile.sys /e/pagefile.sys 2>/dev/null

echo '== jetbrains versions =='
ls -1 "$R/JetBrains" 2>/dev/null; ls -1 "$L/JetBrains" 2>/dev/null
for d in "$R/JetBrains"/* "$L/JetBrains"/*; do sz "$d"; done

echo '== im apps (suggest only) =='
sz "$R/Tencent"; ls -1 "$R/Tencent" 2>/dev/null | head; sz "$U/Documents/WXWork"; sz "$U/Documents/WeChat Files"; sz "$R/xwechat"

echo '== project roots on E =='
ls -1 /e/code 2>/dev/null | head -n 40
echo "node_modules with package.json sibling: $(find /e/code -maxdepth 7 -type d -name node_modules -prune 2>/dev/null | while read -r d; do [ -f "$(dirname "$d")/package.json" ] && echo x; done | wc -l)"
echo "node_modules without sibling: $(find /e/code -maxdepth 7 -type d -name node_modules -prune 2>/dev/null | while read -r d; do [ -f "$(dirname "$d")/package.json" ] || echo x; done | wc -l)"

echo '== docker data disk / fstrim options =='
wsl.exe -l -v 2>&1 | tr -d '\000\r'
timeout 30 wsl.exe -d docker-desktop -u root sh -c 'cat /proc/mounts | grep -Ei "ext4|docker" | head -n 10; command -v fstrim; df -h 2>/dev/null | grep -Ei "sd|docker" | head' 2>&1 | tr -d '\000\r'
wsl.exe --version 2>&1 | tr -d '\000\r' | head -n 3
D='/c/Program Files/Docker/Docker/resources/bin/docker.exe'
timeout 30 "$D" version --format 'docker server={{.Server.Version}}' 2>&1
grep -o '"[A-Za-z]*[Ss]parse[A-Za-z]*"[^,]*' "$R/Docker/settings-store.json" "$R/Docker/settings.json" 2>/dev/null | head
