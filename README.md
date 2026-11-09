# Gift Planner

One master gift list per kid. Each gift is assigned to a **giver group** (e.g. one set of grandparents).
Each group gets a private share link (no login) showing **only** the gifts assigned to it, where they mark gifts bought. No duplicates.

- **Admin (you):** password login → *Gifts* board (add, assign, see status) and *People* (kids, groups, copy/reset each group's link).
  "Show what's been bought" is off by default so kids peeking at the screen see nothing.
- **Family:** open the link you sent them (`/g/<token>`). Anyone with a link can see that group's list; reset it if it leaks.

## Run locally

```
dotnet run --project GiftPlanner      # http://localhost:5098, admin password "devpassword"
dotnet test
```

## Making links work away from home

Family aren't on your LAN, so expose the app over HTTPS (Nginx Proxy Manager + a subdomain, or a Cloudflare Tunnel)
and set `GIFTPLANNER_PUBLIC_URL=https://gifts.example.com` in `~/giftplanner/.env` so copied links use that address.
Only `/g/...` links need to be public; the admin pages stay behind the password.

## Deploy to the Pi

```
docker buildx build --platform linux/arm64 -t giftplanner:latest --load .
docker save giftplanner:latest | gzip -1 | ssh jake@192.168.1.137 'gunzip | docker load'
scp deploy/pi/* jake@192.168.1.137:giftplanner/
ssh -t jake@192.168.1.137 "sudo mkdir -p /mnt/data/giftplanner && sudo chown 1654 /mnt/data/giftplanner && giftplanner/set-password.sh"
```

App: http://192.168.1.137:8087
