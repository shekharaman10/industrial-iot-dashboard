import { createContext, useContext, useState } from "react";
import { dark, light } from "../utils/theme";

const ThemeContext = createContext({ T: dark, isDark: true, toggleTheme: () => {} });
// eslint-disable-next-line react-refresh/only-export-components
export const useTheme = () => useContext(ThemeContext);

export function ThemeProvider({ children }) {
  const [isDark, setIsDark] = useState(true);
  return (
    <ThemeContext.Provider value={{ T: isDark ? dark : light, isDark, toggleTheme: () => setIsDark(d => !d) }}>
      {children}
    </ThemeContext.Provider>
  );
}
