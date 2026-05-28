/** @type {import('tailwindcss').Config} */
module.exports = {
  content: ["./src/**/*.{html,ts}"],
  darkMode: 'class',
  theme: {
    extend: {
      colors: {
        // Brand palette - sourced from Setup.xlsx.
        brand: {
          50:  '#FCEBFF',
          100:  '#F8D6FE',
          200:  '#F2ADFD',
          300:  '#E97AFC',
          400:  '#DF3DFA',
          500:  '#D500F9',
          600:  '#BB00DB',
          700:  '#A200BD',
          800:  '#84009A',
          900:  '#620073',
          950:  '#40004B',
        },
        fuchsia: {
          50:  '#FFF0F5',
          100:  '#FFE0EB',
          200:  '#FFC2D7',
          300:  '#FF9CBD',
          400:  '#FF6E9F',
          500:  '#FF4081',
          600:  '#E03872',
          700:  '#C23162',
          800:  '#9E2850',
          900:  '#751D3B',
          950:  '#4D1327',
        },
        ink: {
          50:  '#f8fafc',
          100: '#f1f5f9',
          200: '#e2e8f0',
          300: '#cbd5e1',
          400: '#94a3b8',
          500: '#64748b',
          600: '#475569',
          700: '#334155',
          800: '#1e293b',
          850: '#172033',
          900: '#0f172a',
          950: '#080d1a',
        },
      },
      fontFamily: {
        sans: ['"Inter"', 'system-ui', 'sans-serif'],
        display: ['"Space Grotesk"', '"Inter"', 'sans-serif'],
      },
      backgroundImage: {
        'gradient-radial': 'radial-gradient(ellipse at top, var(--tw-gradient-stops))',
      },
    },
  },
  plugins: [],
};
