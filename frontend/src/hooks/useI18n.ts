import { useContext } from 'react';
import { I18nContext } from '../i18n/i18n-context';

export const useI18n = () => {
  const ctx = useContext(I18nContext);
  if (ctx === undefined) {
    throw new Error('useI18n must be used within an I18nProvider');
  }
  return ctx;
};
