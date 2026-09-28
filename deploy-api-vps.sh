#!/usr/bin/env bash
# Deploys the ErongoIT.Backup API to the VPS without shipping the whole
# Docker image. Only the published app (a few MB) is uploaded, in small
# resumable pieces, and the VPS builds a thin image on top of the
# existing one.
#
# Run from MobaXterm LOCAL terminal:
#   cd /drives/c/Users/Raymond/source/repos/ErongoIT.Backup
#   bash deploy-api-vps.sh

VPS="root@159.65.18.148"
KEY="$HOME/.ssh/id_ed25519"
REMOTE_DIR="/opt/erongoit-backup"
PART_SIZE=2097152   # 2 MB per piece
SSHOPT="-i $KEY -o ServerAliveInterval=15 -o ServerAliveCountMax=4 -o ConnectTimeout=20"

cd /drives/c/Users/Raymond/source/repos/ErongoIT.Backup || exit 1

echo "=== 1. Publishing API ==="
rm -rf publish-api deploy-parts api-app.tar.gz
dotnet publish ErongoIT.Backup.Api/ErongoIT.Backup.Api.csproj \
  -c Release -o publish-api || { echo "PUBLISH FAILED"; exit 1; }

# Match the Docker build: never ship local/dev settings.
rm -f publish-api/appsettings.Local.json publish-api/appsettings.Development.json

echo "=== 2. Packing ==="
tar -czf api-app.tar.gz -C publish-api . || { echo "TAR FAILED"; exit 1; }
MD5=$(md5sum api-app.tar.gz | awk '{print $1}')
ls -lh api-app.tar.gz
echo "md5: $MD5"

mkdir deploy-parts
split -a 3 -b $PART_SIZE api-app.tar.gz deploy-parts/part-
PARTS=$(ls deploy-parts | wc -l)
echo "Pieces: $PARTS"

echo "=== 3. Uploading ==="
ssh $SSHOPT $VPS "mkdir -p $REMOTE_DIR/deploy-parts" || { echo "SSH FAILED"; exit 1; }

for f in deploy-parts/part-*; do
  name=$(basename "$f")
  localsize=$(stat -c %s "$f")
  ok=0
  for attempt in 1 2 3 4 5 6 7 8 9 10; do
    remotesize=$(ssh $SSHOPT $VPS "stat -c %s $REMOTE_DIR/deploy-parts/$name 2>/dev/null || echo 0")
    if [ "$remotesize" = "$localsize" ]; then echo "$name OK"; ok=1; break; fi
    echo "$name uploading (attempt $attempt)..."
    scp $SSHOPT -q "$f" $VPS:$REMOTE_DIR/deploy-parts/ || sleep 5
  done
  if [ $ok -ne 1 ]; then
    echo "UPLOAD FAILED on $name. Run the script again - finished pieces are skipped."
    exit 1
  fi
done

echo "=== 4. Building and restarting on VPS ==="
ssh $SSHOPT $VPS "bash -s" <<REMOTE
set -e
cd $REMOTE_DIR
cat deploy-parts/part-* > api-app.tar.gz
RECEIVED=\$(md5sum api-app.tar.gz | cut -d' ' -f1)
echo "expected: $MD5"
echo "received: \$RECEIVED"
[ "\$RECEIVED" = "$MD5" ] || { echo "CHECKSUM MISMATCH"; exit 1; }

rm -rf api-build && mkdir -p api-build/app
tar -xzf api-app.tar.gz -C api-build/app

cat > api-build/Dockerfile <<'DOCKERFILE'
FROM erongoit-backup-api:previous
WORKDIR /app
COPY app/ /app/
ENTRYPOINT ["dotnet", "ErongoIT.Backup.Api.dll"]
DOCKERFILE

# Keep the running image as a rollback point (only the first time,
# so we don't stack layers on every deploy).
docker image inspect erongoit-backup-api:previous >/dev/null 2>&1 || \
  docker tag erongoit-backup-api:latest erongoit-backup-api:previous

docker build -t erongoit-backup-api:latest api-build
docker compose up -d --no-deps --force-recreate api

rm -rf deploy-parts api-build api-app.tar.gz

set +e
sleep 3
docker ps --format "table {{.Names}}\t{{.Status}}"
echo "--- last API log lines ---"
docker logs erongoit-backup-api --tail 8 2>&1
REMOTE

echo "=== 5. Checking public health (https://backup.erongoit.com) ==="
HEALTH=""
for i in $(seq 1 30); do
  HEALTH=$(curl -s -m 5 https://backup.erongoit.com/api/health)
  echo "$HEALTH" | grep -q '"Healthy"' && break
  HEALTH=""
  sleep 2
done
echo "$HEALTH"

rm -rf publish-api deploy-parts api-app.tar.gz
[ -n "$HEALTH" ] && echo "=== DEPLOY COMPLETE ===" || echo "=== DEPLOY FAILED (API not healthy) ==="
