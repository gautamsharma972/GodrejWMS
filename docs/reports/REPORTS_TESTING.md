# Reports testing

Report tests cover aggregation, warehouse access, unauthorized callers, filters, sorting, pagination, partial putaway completion, pullout confirmation behavior, and the existing specialist report calculations.

For production validation, run the test suite and inspect generated SQL/query plans against representative MySQL data. Verify each export against its filtered grid and confirm the 30-second refresh preserves report selection and paging.
