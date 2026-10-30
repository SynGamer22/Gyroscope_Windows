create extension if not exists pgcrypto;

create table if not exists pairing_sessions (
    id uuid primary key default gen_random_uuid(),
    code text not null unique,
    created_at timestamptz not null default now(),
    expires_at timestamptz not null,
    owner_id text not null,
    is_active boolean not null default true,
    state jsonb not null default '{}'::jsonb,
    last_seen_at timestamptz not null default now()
);

create table if not exists signaling_messages (
    id uuid primary key default gen_random_uuid(),
    session_id uuid not null references pairing_sessions(id) on delete cascade,
    sender_id text not null,
    recipient_id text,
    kind text not null check (kind in ('offer','answer','ice-candidate','status','ping')),
    payload jsonb not null default '{}'::jsonb,
    created_at timestamptz not null default now()
);

create index if not exists pairing_sessions_code_idx on pairing_sessions(code);
create index if not exists pairing_sessions_expires_idx on pairing_sessions(expires_at);
create index if not exists signaling_session_idx on signaling_messages(session_id, created_at desc);

alter table pairing_sessions enable row level security;
alter table signaling_messages enable row level security;

create policy "allow creating pairing sessions"
on pairing_sessions for insert
with check (true);

create policy "allow read own pairing session"
on pairing_sessions for select
using (owner_id = current_setting('request.jwt.claims', true)::jsonb->>'sub' or owner_id = 'anonymous');

create policy "allow updating own pairing session"
on pairing_sessions for update
using (owner_id = current_setting('request.jwt.claims', true)::jsonb->>'sub' or owner_id = 'anonymous')
with check (owner_id = current_setting('request.jwt.claims', true)::jsonb->>'sub' or owner_id = 'anonymous');

create policy "allow delete own pairing session"
on pairing_sessions for delete
using (owner_id = current_setting('request.jwt.claims', true)::jsonb->>'sub' or owner_id = 'anonymous');

create policy "allow inserting signaling messages"
on signaling_messages for insert
with check (
    exists (
        select 1
        from pairing_sessions ps
        where ps.id = session_id
          and ps.is_active = true
          and ps.expires_at > now()
    )
);

create policy "allow read session messages"
on signaling_messages for select
using (
    exists (
        select 1
        from pairing_sessions ps
        where ps.id = session_id
          and ps.is_active = true
          and ps.expires_at > now()
    )
);

create or replace function cleanup_expired_sessions()
returns void
language sql
as $$
    delete from signaling_messages sm
    where sm.session_id in (
        select id from pairing_sessions where expires_at <= now()
    );

    delete from pairing_sessions
    where expires_at <= now();
$$;

create or replace function create_pairing_session(p_code text, p_owner_id text, p_ttl_seconds integer default 900)
returns uuid
language plpgsql
as $$
declare
    v_id uuid;
begin
    insert into pairing_sessions (code, owner_id, expires_at, state)
    values (p_code, p_owner_id, now() + make_interval(secs => p_ttl_seconds), jsonb_build_object('paired', false))
    returning id into v_id;

    return v_id;
end;
$$;
