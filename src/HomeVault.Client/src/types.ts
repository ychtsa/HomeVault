export interface Resident {
  username: string;
  residentName: string;
  residentAddress: string;
  catalogId: string;
}

export interface CatalogItem {
  itemId: string;
  itemName: string;
  itemType: string;
  itemValue: number;
  imageUrl: string | null;
}

export interface ItemDraft {
  itemName: string;
  itemType: string;
  itemValue: number;
}

export interface SignupData {
  residentName: string;
  residentAddress: string;
  username: string;
  email: string;
  password: string;
  confirmPassword: string;
}
