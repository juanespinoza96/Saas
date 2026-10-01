/** @type {import('tailwindcss').Config} */
export default {
  content: [
    "./index.html",
    "./src/**/*.{js,ts,jsx,tsx}",
  ],
  darkMode: 'class',
  theme: {
    // Breakpoints responsive del POS: tablet (768px) y desktop (1024px)
    screens: {
      'sm': '640px',
      'md': '768px',   // Tablet — layout POS funcional completo
      'lg': '1024px',  // Desktop — layout con columnas lado a lado
      'xl': '1280px',
      '2xl': '1536px',
    },
    extend: {
      colors: {
        // Semantic action colors (pastel/desaturated, saturation ≤ 70% HSL)
        'action-confirm': '#4CAF7D',
        'action-edit': '#F5A623',
        'action-danger': '#E57373',
        // Light theme
        'light-bg': '#F8F9FA',
        // Dark theme
        'dark-bg': '#1E1E2E',
        'dark-surface': '#2A2A3C',
        'dark-text': '#E4E4E7',
      },
      // Tamaño mínimo de área de toque según WCAG 2.1 (44x44px)
      minWidth: {
        'touch': '44px',
      },
      minHeight: {
        'touch': '44px',
      },
      // Tamaños de fuente mínimos para legibilidad en pantallas de alta densidad
      fontSize: {
        'content-min': ['14px', { lineHeight: '1.5' }],
        'label-min': ['12px', { lineHeight: '1.4' }],
      },
    },
  },
  plugins: [],
}
