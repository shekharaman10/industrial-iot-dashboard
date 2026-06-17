import js from "@eslint/js";
import globals from "globals";
import reactPlugin from "eslint-plugin-react";
import reactHooks from "eslint-plugin-react-hooks";
import reactRefresh from "eslint-plugin-react-refresh";

export default [
  { ignores: ["dist", "node_modules"] },
  {
    files: ["**/*.{js,jsx}"],
    languageOptions: {
      ecmaVersion: 2020,
      globals: globals.browser,
      parserOptions: {
        ecmaVersion: "latest",
        ecmaFeatures: { jsx: true },
        sourceType: "module",
      },
    },
    plugins: {
      "react"         : reactPlugin,
      "react-hooks"   : reactHooks,
      "react-refresh" : reactRefresh,
    },
    rules: {
      ...reactHooks.configs.recommended.rules,
      "react/jsx-uses-vars"                  : "error",
      "react/jsx-uses-react"                 : "error",
      "react-refresh/only-export-components" : ["warn", { allowConstantExport: true }],
      "no-unused-vars"   : ["warn", { argsIgnorePattern: "^_" }],
      "no-console"       : ["warn", { allow: ["error", "warn"] }],
      "prefer-const"     : "error",
      "no-var"           : "error",
    },
  },
];
