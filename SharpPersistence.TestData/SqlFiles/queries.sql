-- #start# GetAllUsers
SELECT *
FROM users
-- #end# GetAllUsers

-- #start# GetActiveUsers
SELECT *
FROM users
WHERE active = 1
-- #end# GetActiveUsers


SELECT 'this will not be parsed';
