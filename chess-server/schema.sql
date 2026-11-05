-- Run in the Supabase SQL editor. Server uses the service_role key only.
create table if not exists public.game_users (
  id uuid primary key default gen_random_uuid(),
  username text not null,
  email text not null,
  password_hash text not null,
  salt text not null,
  is_verified boolean not null default false,
  verification_code text,
  verification_code_expiry bigint,
  created_at timestamptz not null default now()
);
create unique index if not exists game_users_username_lower on public.game_users (lower(username));
create unique index if not exists game_users_email_lower on public.game_users (lower(email));
alter table public.game_users enable row level security;
revoke all on public.game_users from anon, authenticated;
grant select, insert, update, delete on public.game_users to service_role;
