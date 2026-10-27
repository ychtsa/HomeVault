import type { CatalogItem } from './types';

export type SortOrder = 'name' | 'value-desc' | 'value-asc';
export const currency = (amount: number) => new Intl.NumberFormat('en-US', {
  style: 'currency', currency: 'USD', maximumFractionDigits: 0,
}).format(amount);

export function selectItems(items: CatalogItem[], search: string, category: string, sort: SortOrder): CatalogItem[] {
  const term = search.trim().toLocaleLowerCase();
  return items.filter(item => (!category || item.itemType === category)
    && (!term || `${item.itemName} ${item.itemType}`.toLocaleLowerCase().includes(term)))
    .sort((a, b) => sort === 'value-desc' ? b.itemValue - a.itemValue
      : sort === 'value-asc' ? a.itemValue - b.itemValue
        : a.itemName.localeCompare(b.itemName, 'zh-Hant'));
}

export function summarizeItems(items: CatalogItem[]) {
  return {
    count: items.length,
    totalValue: items.reduce((total, item) => total + item.itemValue, 0),
    photoCount: items.filter(item => item.imageUrl).length,
    categories: [...new Set(items.map(item => item.itemType))].sort((a, b) => a.localeCompare(b, 'zh-Hant')),
  };
}

export function validatePhoto(file: File): string | null {
  if (!['image/jpeg', 'image/png', 'image/webp'].includes(file.type)) return '請選擇 JPG、PNG 或 WebP 圖片。';
  if (file.size > 5 * 1024 * 1024) return '照片大小不能超過 5 MB。';
  return null;
}
