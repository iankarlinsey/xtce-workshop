// Custom launcher for running Karma tests headless in a container (CI, or local
// docker-based verification) — plain ChromeHeadless fails under a root/no-namespace
// container user with a sandbox error, so this disables the sandbox explicitly.
process.env.CHROME_BIN = process.env.CHROME_BIN || '/usr/bin/chromium';

module.exports = function (config) {
  config.set({
    // Angular's @angular/build:karma only injects its default frameworks: ['jasmine']
    // when no custom karmaConfig is supplied at all — providing this file (for the
    // custom launcher below) silently drops that default, so it must be repeated here
    // or specs fail at runtime with "describe is not defined".
    frameworks: ['jasmine'],
    customLaunchers: {
      ChromeHeadlessNoSandbox: {
        base: 'ChromeHeadless',
        flags: ['--no-sandbox', '--disable-gpu', '--disable-dev-shm-usage'],
      },
    },
    browsers: ['ChromeHeadlessNoSandbox'],
    // Coverage (issue #55): active when `ng test --code-coverage` runs. Like the
    // frameworks default above, the builder's coverage wiring is dropped by a custom
    // config, so the reporter and its ratchet thresholds live here. Floors sit under
    // the measured baseline so coverage can only rise; raise them deliberately.
    coverageReporter: {
      dir: require('path').join(__dirname, 'coverage'),
      subdir: '.',
      reporters: [{ type: 'text-summary' }, { type: 'html' }, { type: 'lcovonly' }],
      check: {
        global: {
          statements: 80,
          lines: 80,
          branches: 65,
          functions: 80,
        },
      },
    },
  });
};
