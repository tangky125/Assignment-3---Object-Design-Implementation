// Coding standard for the web UI: the official Vue style guide ("recommended" rules, which include
// the essential and strongly-recommended priorities), typescript-eslint recommended rules, and
// Prettier for layout. Check with: npm run lint
import pluginVue from 'eslint-plugin-vue'
import vueTsEslintConfig from '@vue/eslint-config-typescript'
import skipFormatting from '@vue/eslint-config-prettier/skip-formatting'

export default [
  { name: 'app/files-to-lint', files: ['**/*.{ts,mts,tsx,vue}'] },
  { name: 'app/files-to-ignore', ignores: ['**/dist/**', '**/node_modules/**', '../wwwroot/**'] },
  ...pluginVue.configs['flat/recommended'],
  ...vueTsEslintConfig({ extends: ['recommended'] }),
  {
    name: 'app/project-rules',
    rules: {
      'vue/block-lang': ['error', { script: { lang: 'ts' } }],
      'vue/component-api-style': ['error', ['script-setup']],
      'vue/define-props-declaration': ['error', 'type-based'],
      'vue/require-default-prop': 'off',
      eqeqeq: ['error', 'always'],
      'no-console': 'error',
      '@typescript-eslint/explicit-function-return-type': ['error', { allowExpressions: true }],
    },
  },
  skipFormatting,
]
