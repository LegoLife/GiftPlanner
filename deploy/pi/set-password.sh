#!/bin/sh
# Sets (or changes) the GiftPlanner login password. Only its hash is stored in .env.
set -e
cd "$(dirname "$0")"

printf "New GiftPlanner password: "; stty -echo; read -r PW; stty echo; echo
printf "Type it again: "; stty -echo; read -r PW2; stty echo; echo
[ "$PW" = "$PW2" ] || { echo "The passwords don't match."; exit 1; }
[ ${#PW} -ge 8 ] || { echo "Please use at least 8 characters."; exit 1; }

HASH=$(printf '%s\n' "$PW" | docker run --rm -i giftplanner:latest hash-password 2>/dev/null | tail -n 1)
[ -n "$HASH" ] || { echo "Couldn't create the password hash."; exit 1; }

touch .env && chmod 600 .env
grep -v '^GIFTPLANNER_PASSWORD_HASH=' .env > .env.tmp || true
printf 'GIFTPLANNER_PASSWORD_HASH=%s\n' "$HASH" >> .env.tmp
mv .env.tmp .env && chmod 600 .env

docker compose up -d giftplanner
echo "Password set. Open http://$(hostname -I | cut -d' ' -f1):8087"
