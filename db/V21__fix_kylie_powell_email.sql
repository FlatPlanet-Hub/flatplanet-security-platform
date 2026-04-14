-- V21: Fix Kylie Powell email — add .au suffix
UPDATE platform.users
SET email = 'kylie.powell@flatplanet.com.au'
WHERE email = 'kylie.powell@flatplanet.com';
