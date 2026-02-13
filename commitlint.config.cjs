module.exports = {
  extends: ['@commitlint/config-conventional'],
  rules: {
    'subject-case': [2, 'never', ['pascal-case']],
    // Enforce concise subjects for better changelog readability
    'subject-max-length': [2, 'always', 100],
    'subject-min-length': [2, 'always', 5],
  },
};
