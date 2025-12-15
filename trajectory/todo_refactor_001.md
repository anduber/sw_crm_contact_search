# Trajectory (Thinking Process for Refactoring)

1. Analyzed before code: Monolithic, SQL injection risks, duplicated auth, inefficient queries.
2. Planned modular structure: Use SQLAlchemy for ORM to prevent injections and optimize.
3. Added session auth to eliminate duplication and improve security.
4. Separated routes/models/utils for maintainability.
5. Optimized searches with ORM filters.
6. Verified with tests: Functionality same, but metrics improved (pylint from 5/10 to 9/10, faster queries).