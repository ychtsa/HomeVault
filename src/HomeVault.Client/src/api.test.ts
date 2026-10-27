import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { api, ApiError, resetCsrf } from './api';

const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });
const fetchMock = vi.fn<typeof fetch>();
beforeEach(() => { resetCsrf(); fetchMock.mockReset(); vi.stubGlobal('fetch', fetchMock); });
afterEach(() => vi.unstubAllGlobals());

describe('cookie API client', () => {
  it('refreshes the CSRF token after login and logout', async () => {
    fetchMock.mockResolvedValueOnce(json({ token: 'anonymous' })).mockResolvedValueOnce(json({ username: 'mei' }))
      .mockResolvedValueOnce(json({ token: 'authenticated' })).mockResolvedValueOnce(new Response(null, { status: 204 }))
      .mockResolvedValueOnce(json({ token: 'new-anonymous' })).mockResolvedValueOnce(json({ username: 'mei' }));
    await api.login('mei', 'password1');
    await api.logout();
    await api.login('mei', 'password1');
    expect(fetchMock.mock.calls.filter(([url]) => url === '/api/auth/csrf')).toHaveLength(3);
    expect(new Headers(fetchMock.mock.calls[1][1]?.headers).get('X-CSRF-TOKEN')).toBe('anonymous');
    expect(new Headers(fetchMock.mock.calls[3][1]?.headers).get('X-CSRF-TOKEN')).toBe('authenticated');
    expect(fetchMock.mock.calls[1][1]?.credentials).toBe('same-origin');
  });
  it('refreshes identity-bound CSRF after a session expires', async () => {
    fetchMock.mockResolvedValueOnce(json({ token: 'old-session' })).mockResolvedValueOnce(json({ username: 'mei' }))
      .mockResolvedValueOnce(json({ token: 'authenticated' })).mockResolvedValueOnce(json({}, 401))
      .mockResolvedValueOnce(json({ token: 'anonymous-after-expiry' })).mockResolvedValueOnce(json({ username: 'mei' }));
    await api.login('mei', 'password1');
    await expect(api.createItem({ itemName: 'Camera', itemType: 'Electronics', itemValue: 100 })).rejects.toMatchObject({ status: 401 });
    await api.login('mei', 'password1');
    expect(new Headers(fetchMock.mock.calls[5][1]?.headers).get('X-CSRF-TOKEN')).toBe('anonymous-after-expiry');
  });
  it('lets the browser supply a multipart boundary and still supplies CSRF', async () => {
    fetchMock.mockResolvedValueOnce(json({ token: 'safe-token' })).mockResolvedValueOnce(json({ itemId: '00001' }));
    await api.uploadImage('00001', new File(['photo'], 'photo.png', { type: 'image/png' }));
    const options = fetchMock.mock.calls[1][1]!;
    expect(new Headers(options.headers).get('Content-Type')).toBeNull();
    expect(new Headers(options.headers).get('X-CSRF-TOKEN')).toBe('safe-token');
    expect((options.body as FormData).get('file')).toBeInstanceOf(File);
  });
  it('shows API validation errors and retains status for session-expiry handling', async () => {
    fetchMock.mockResolvedValueOnce(json({ errors: { ItemValue: ['Value must be positive.'] } }, 400));
    await expect(api.items()).rejects.toMatchObject({ message: 'Value must be positive.', status: 400 });
    fetchMock.mockResolvedValueOnce(new Response('<html>unauthorized</html>', { status: 401 }));
    await expect(api.me()).rejects.toBeInstanceOf(ApiError);
  });
});
