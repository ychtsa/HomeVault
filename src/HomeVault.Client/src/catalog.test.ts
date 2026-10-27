import { describe, expect, it } from 'vitest';
import { selectItems, summarizeItems, validatePhoto } from './catalog';
import type { CatalogItem } from './types';

const items: CatalogItem[] = [
  { itemId: '00001', itemName: 'Canon camera', itemType: '電子產品', itemValue: 750, imageUrl: '/api/items/00001/image' },
  { itemId: '00002', itemName: '沙發', itemType: '家具', itemValue: 1600, imageUrl: null },
  { itemId: '00003', itemName: '木椅', itemType: '家具', itemValue: 300, imageUrl: null },
];

describe('catalog selection', () => {
  it('combines category and case-insensitive search without mutating the inventory', () => {
    expect(selectItems(items, '  CANON ', '電子產品', 'name').map(item => item.itemId)).toEqual(['00001']);
    expect(selectItems(items, 'Canon', '家具', 'name')).toEqual([]);
    expect(selectItems(items, '', '家具', 'value-desc').map(item => item.itemValue)).toEqual([1600, 300]);
    expect(items.map(item => item.itemValue)).toEqual([750, 1600, 300]);
  });
  it('calculates real totals and zero totals for an empty account', () => {
    expect(summarizeItems(items)).toMatchObject({ count: 3, totalValue: 2650, photoCount: 1 });
    expect(summarizeItems(items).categories).toHaveLength(2);
    expect(summarizeItems([])).toEqual({ count: 0, totalValue: 0, photoCount: 0, categories: [] });
  });
  it('rejects unsupported and oversized photo uploads', () => {
    expect(validatePhoto(new File(['x'], 'image.svg', { type: 'image/svg+xml' }))).toContain('JPG');
    const oversized = new File(['x'], 'image.png', { type: 'image/png' });
    Object.defineProperty(oversized, 'size', { value: 5 * 1024 * 1024 + 1 });
    expect(validatePhoto(oversized)).toContain('5 MB');
    expect(validatePhoto(new File(['x'], 'image.webp', { type: 'image/webp' }))).toBeNull();
  });
});
