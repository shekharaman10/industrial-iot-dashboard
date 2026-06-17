/**
 * theme.js
 * Design tokens for dark (Emerson Ovation DCS) and light (NI DataView) themes.
 */

export const dark = {
  name: 'dark',
  bg0: '#020c18', bg1: '#071525', bg2: '#0b1d30', bg3: '#112438',
  border: '#1a3855', borderAccent: '#1a76bb',
  text0: '#d8ecf8', text1: '#6b90ad', text2: '#3d6280',
  brand: '#00a651',        // Emerson green
  accent: '#0d7ec9',       // NI blue accent
  accentLight: '#29a9ff',
  online: '#00c896', offline: '#445e75', stale: '#e8920c',
  info: '#3b9de0', warning: '#e8920c', critical: '#e8500c', fault: '#e82c2c',
  vib: '#e8920c', vibFill: 'rgba(232,146,12,0.12)',
  avg: '#3b9de0', temp: '#00c896', humid: '#a78bfa', anomaly: '#e82c2c',
  chartGrid: '#1a3855', chartTick: '#3d6280',
  shadow: '0 4px 20px rgba(0,0,0,0.5)', shadowSm: '0 2px 8px rgba(0,0,0,0.4)',
};

export const light = {
  name: 'light',
  bg0: '#edf2f7', bg1: '#ffffff', bg2: '#f5f8fc', bg3: '#ebf0f7',
  border: '#c8d8e8', borderAccent: '#0078d4',
  text0: '#0d1f30', text1: '#3a5570', text2: '#7a9ab0',
  brand: '#0078d4',        // NI blue (brand color in light mode)
  accent: '#0078d4',
  accentLight: '#0091ea',
  online: '#007050', offline: '#7a9ab0', stale: '#a06800',
  info: '#0078d4', warning: '#a06800', critical: '#882800', fault: '#a81818',
  vib: '#0078d4', vibFill: 'rgba(0,120,212,0.10)',
  avg: '#007050', temp: '#007050', humid: '#5b4dbb', anomaly: '#a81818',
  chartGrid: '#dde8f0', chartTick: '#7a9ab0',
  shadow: '0 2px 12px rgba(0,0,0,0.08)', shadowSm: '0 1px 4px rgba(0,0,0,0.08)',
};
