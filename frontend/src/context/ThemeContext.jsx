import { createContext, useContext, useState, useEffect } from "react";
import { dark, light } from "../utils/theme";

const ThemeContext = createContext({ T: dark, isDark: true, toggleTheme: () => {} });
// eslint-disable-next-line react-refresh/only-export-components
export const useTheme = () => useContext(ThemeContext);

export function ThemeProvider({ children }) {
  const [isDark, setIsDark] = useState(() => {
    try { return localStorage.getItem("iot-theme") !== "light"; }
    catch { return true; }
  });

  useEffect(() => {
    try { localStorage.setItem("iot-theme", isDark ? "dark" : "light"); }
    catch {}
    document.documentElement.style.background = isDark ? dark.bg0 : light.bg0;
  }, [isDark]);

  return (
    <ThemeContext.Provider value={{ T: isDark ? dark : light, isDark, toggleTheme: () => setIsDark(d => !d) }}>
      {children}
    </ThemeContext.Provider>
  );
}
