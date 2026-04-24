const path = require('path');
const reload = require('require-nocache')(module);

const cssVarsFiles = [
  './src/Styles/Variables/dimensions',
  './src/Styles/Variables/fonts',
  './src/Styles/Variables/animations',
  './src/Styles/Variables/zIndexes'
].map(require.resolve);

const mixinsFiles = [
  path.join(__dirname, 'src/Styles/Mixins/colorImpairedGradients.css'),
  path.join(__dirname, 'src/Styles/Mixins/cover.css'),
  path.join(__dirname, 'src/Styles/Mixins/linkOverlay.css'),
  path.join(__dirname, 'src/Styles/Mixins/scroller.css'),
  path.join(__dirname, 'src/Styles/Mixins/truncate.css')
];

module.exports = {
  plugins: [
    ['postcss-mixins', {
      mixinsFiles
    }],
    ['postcss-simple-vars', {
      variables: () =>
        cssVarsFiles.reduce((acc, vars) => {
          return Object.assign(acc, reload(vars));
        }, {})
    }],
    '@csstools/postcss-color-function',
    'postcss-nested'
  ]
};
