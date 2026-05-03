const base = require('@camunda/sdk-infra/configs/commitlint.config.base.cjs');
module.exports = {
  ...base,
  rules: {
    ...base.rules,
    // Allow long body lines (e.g. dependency update details)
    'body-max-line-length': [2, 'always', 500],
  },
};
