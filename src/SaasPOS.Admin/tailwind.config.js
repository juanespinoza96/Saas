/** @type {import('tailwindcss').Config} */
export default {
  content: [
    "./index.html",
    "./src/**/*.{js,ts,jsx,tsx}",
  ],
  darkMode: 'class',
  theme: {
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
    },
  },
  plugins: [],
}
