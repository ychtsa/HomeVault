import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import App from './App';
import { api, ApiError } from './api';
import type { CatalogItem } from './types';

vi.mock('./api', async importOriginal => {
  const actual = await importOriginal<typeof import('./api')>();
  return { ...actual, api: { me: vi.fn(), items: vi.fn(), login: vi.fn(), signup: vi.fn(), logout: vi.fn(), createItem: vi.fn(), updateItem: vi.fn(), deleteItem: vi.fn(), uploadImage: vi.fn(), removeImage: vi.fn(), report: vi.fn() } };
});

const resident = { username: 'mei', residentName: '小美', residentAddress: '台北', catalogId: '00001' };
const item: CatalogItem = { itemId: '00001', itemName: '相機', itemType: '電子產品', itemValue: 750, imageUrl: null };
beforeEach(() => {
  vi.resetAllMocks();
  vi.mocked(api.me).mockResolvedValue(resident);
  vi.mocked(api.items).mockResolvedValue([]);
});

describe('home inventory', () => {
  it('shows an empty account without fabricated inventory', async () => {
    render(<App />);
    expect(await screen.findByText('你的第一件珍貴，從這裡開始。')).toBeInTheDocument();
    expect(screen.getByText('$0')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: '新增第一件物品' })).toBeEnabled();
  });
  it('uses the API login and keeps API failures visible', async () => {
    vi.mocked(api.me).mockRejectedValue(new ApiError('Unauthorized', 401));
    vi.mocked(api.login).mockRejectedValueOnce(new ApiError('帳號或密碼不正確。', 401)).mockResolvedValueOnce(resident);
    const user = userEvent.setup();
    render(<App />);
    await user.type(await screen.findByLabelText('使用者名稱'), 'mei');
    await user.type(screen.getByLabelText('密碼'), 'password1');
    await user.click(screen.getByRole('button', { name: '進入我的 HomeVault' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('帳號或密碼不正確。');
    await user.click(screen.getByRole('button', { name: '進入我的 HomeVault' }));
    expect(await screen.findByText('你的第一件珍貴，從這裡開始。')).toBeInTheDocument();
    expect(api.login).toHaveBeenCalledWith('mei', 'password1');
  });
  it('filters rendered items and clears an unsuccessful search', async () => {
    vi.mocked(api.items).mockResolvedValue([item, { ...item, itemId: '00002', itemName: '沙發', itemType: '家具', itemValue: 1500 }]);
    const user = userEvent.setup();
    render(<App />);
    await screen.findByRole('button', { name: '相機' });
    await user.type(screen.getByRole('searchbox'), '不存在');
    expect(screen.getByText('還沒找到符合的物品')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: '清除搜尋與篩選' }));
    expect(screen.getByRole('button', { name: '相機' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: '沙發' })).toBeInTheDocument();
  });
  it('keeps a newly created item ID when photo upload fails so retry cannot duplicate it', async () => {
    vi.mocked(api.createItem).mockResolvedValue(item);
    vi.mocked(api.updateItem).mockResolvedValue(item);
    vi.mocked(api.uploadImage).mockRejectedValueOnce(new ApiError('Storage unavailable', 503)).mockResolvedValueOnce({ ...item, imageUrl: '/api/items/00001/image' });
    const user = userEvent.setup();
    render(<App />);
    await user.click(await screen.findByRole('button', { name: '新增第一件物品' }));
    const dialog = screen.getByRole('dialog');
    await user.type(within(dialog).getByLabelText('物品名稱'), '相機');
    await user.type(within(dialog).getByLabelText('物品類別'), '電子產品');
    await user.type(within(dialog).getByLabelText('預估價值（USD）'), '750');
    const file = new File(['image'], 'camera.png', { type: 'image/png' });
    await user.upload(within(dialog).getByLabelText('上傳物品照片'), file);
    await user.click(within(dialog).getByRole('button', { name: '儲存物品' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('物品資料已儲存，但照片更新失敗');
    expect(api.createItem).toHaveBeenCalledTimes(1);
    await user.click(screen.getByRole('button', { name: '儲存物品' }));
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(api.createItem).toHaveBeenCalledTimes(1);
    expect(api.updateItem).toHaveBeenCalledWith('00001', { itemName: '相機', itemType: '電子產品', itemValue: 750 });
    expect(api.uploadImage).toHaveBeenCalledTimes(2);
  });
  it('requires a confirmation before deleting an item', async () => {
    vi.mocked(api.items).mockResolvedValue([item]);
    vi.mocked(api.deleteItem).mockResolvedValue();
    const user = userEvent.setup();
    render(<App />);
    await user.click(await screen.findByRole('button', { name: '移除 相機' }));
    expect(api.deleteItem).not.toHaveBeenCalled();
    fireEvent.keyDown(document, { key: 'Escape' });
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: '移除 相機' }));
    await user.click(screen.getByRole('button', { name: '確認移除' }));
    await waitFor(() => expect(api.deleteItem).toHaveBeenCalledWith('00001'));
    expect(await screen.findByText('你的第一件珍貴，從這裡開始。')).toBeInTheDocument();
  });
});
