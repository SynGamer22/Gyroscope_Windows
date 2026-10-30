# Supabase setup for MotionLink VR

This project uses Supabase for pairing and WebRTC signaling only. Motion packets travel via the WebRTC DataChannel after the peer connection is established.

## 1. Create a Supabase project

1. Create a new Supabase project.
2. Open the SQL editor.
3. Paste the contents of `schema.sql`.
4. Run it once.

## 2. Set environment variables

Copy the example file at the repository root:

```bash
cp .env.example .env
```

Then update the values:

- `SUPABASE_URL` = your project URL, for example `https://xyzcompany.supabase.co`
- `SUPABASE_PUBLISHABLE_KEY` = your public anonymous key from Project Settings > API

Do not add a service-role or secret key to the browser app or the repository.

## 3. Security notes

The project is designed to avoid storing motion data in Supabase. Pairing metadata is kept minimal and sessions expire automatically.

The schema intentionally uses minimal RBAC. In production, enforce Row Level Security and keep the publishable key restricted to anonymous client access only.

## 4. Local development

You may run a local HTTPS host for the mobile app, or serve the app via GitHub Pages or another HTTPS static host. If you test locally, use a trusted certificate and ensure the browser allows motion sensor access over secure origins.

## 5. WebRTC signaling

The mobile app and desktop app exchange offers, answers, and ICE candidates through a minimal Supabase table. This avoids sending every motion update through the backend while keeping the pairing flow simple.

The actual motion stream is sent over the DataChannel only after the WebRTC connection is established.
