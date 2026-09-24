import vue from "eslint-plugin-vue";
import accessibility from "eslint-plugin-vuejs-accessibility";
import typescript from "typescript-eslint";

export default typescript.config(
    {
        ignores: [
            "dist",
            "coverage",
            "playwright-report",
            "test-results",
            "src/shared/api/generated",
        ],
    },
    ...typescript.configs.recommended,
    ...vue.configs["flat/recommended"],
    ...accessibility.configs["flat/recommended"],
    {
        files: ["**/*.vue"],
        languageOptions: {
            parserOptions: { parser: typescript.parser },
        },
    },
    {
        rules: {
            "@typescript-eslint/no-explicit-any": "error",
            "@typescript-eslint/consistent-type-imports": "error",
            "vue/multi-word-component-names": "off",

            // Raw HTML from anywhere bypasses Vue's escaping. Nothing here needs it.
            "vue/no-v-html": "error",

            // Formatting belongs to Prettier; two tools arguing about line breaks is noise.
            "vue/singleline-html-element-content-newline": "off",
            "vue/multiline-html-element-content-newline": "off",
            "vue/max-attributes-per-line": "off",
            "vue/html-self-closing": "off",
            "vue/html-indent": "off",
            "vue/html-closing-bracket-newline": "off",
            "vue/attributes-order": "off",

            // A label either wraps its control or names it with for; either is a complete association.
            "vuejs-accessibility/label-has-for": [
                "error",
                { required: { some: ["nesting", "id"] }, allowChildren: false },
            ],
        },
    },
);
