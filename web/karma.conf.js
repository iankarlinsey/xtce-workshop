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
  });
};
