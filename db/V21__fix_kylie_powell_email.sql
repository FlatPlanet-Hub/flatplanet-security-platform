-- V21: Fix Kylie Powell email — add .au suffix
UPDATE users
SET email = 'kylie.powell@flatplanet.com.au'
WHERE email = 'kylie.powell@flatplanet.com';
