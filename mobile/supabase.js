export async function createSupabaseClient({ url, anonKey }) {
  if (!url || !anonKey) {
    throw new Error('Supabase URL and publishable key are required.');
  }

  const { createClient } = await import('https://cdn.jsdelivr.net/npm/@supabase/supabase-js@2/+esm');
  return createClient(url, anonKey, {
    auth: { persistSession: false, autoRefreshToken: false },
    global: { headers: { 'x-client-info': 'motionlink-vr-mobile' } }
  });
}

export async function fetchSessionInfo(supabase, code) {
  const { data, error } = await supabase
    .from('pairing_sessions')
    .select('*')
    .eq('code', code)
    .gt('expires_at', new Date().toISOString())
    .maybeSingle();

  if (error) throw error;
  return data;
}

export async function insertSignal(supabase, sessionId, payload) {
  const { data, error } = await supabase.from('signaling_messages').insert([
    { session_id: sessionId, sender_id: 'mobile', kind: payload.kind, payload }
  ]);

  if (error) throw error;
  return data;
}
