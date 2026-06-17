import { ThemeProvider } from "./context/ThemeContext";
import Dashboard from "./components/Dashboard";

export default function App() {
  return (
    <ThemeProvider>
      <Dashboard />
    </ThemeProvider>
  );
}
