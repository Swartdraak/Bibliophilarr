const path = require('path');

module.exports = {
  rootDir: __dirname,
  testEnvironment: 'jsdom',
  roots: ['<rootDir>/frontend/src'],
  testMatch: ['**/*.test.js'],
  moduleDirectories: [
    'node_modules',
    '<rootDir>/frontend/src',
    '<rootDir>/frontend/src/Shims'
  ],
  moduleNameMapper: {
    '^jquery$': 'jquery/dist/jquery.min',
    '^react-middle-truncate$': 'react-middle-truncate/lib/react-middle-truncate',
    '\\.(css)$': 'identity-obj-proxy',
    '\\.(gif|ttf|eot|svg|png|jpg|jpeg|webp)$': '<rootDir>/frontend/build/jestFileMock.js'
  },
  transform: {
    '^.+\\.[jt]sx?$': ['babel-jest', { configFile: path.join(__dirname, 'frontend/babel.config.js'), envName: 'test' }]
  },
  setupFilesAfterEnv: ['<rootDir>/frontend/build/jest.setup.js'],
  transformIgnorePatterns: ['/node_modules/']
};