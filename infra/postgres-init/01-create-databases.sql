-- Database-per-service: Shortening (source of truth for the links) and KGS (counter/ranges).
-- migris creates the objects/tables inside each database; here we only ensure the databases exist.
CREATE DATABASE urlshortener_shortening;
CREATE DATABASE urlshortener_kgs;
