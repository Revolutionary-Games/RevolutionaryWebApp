-- Performs post rails to blazor migration things on the database --
BEGIN TRANSACTION;

DELETE FROM users WHERE local = TRUE;

COMMIT TRANSACTION;
