SELECT v0.title, v0.permalink, v0.published_at
FROM (
         SELECT v.title, v.permalink, v.published_at
         FROM versioned_pages AS v
         WHERE v.published_at IS NOT NULL AND v.visibility = 1 AND v.type = 2 AND NOT (v.deleted) AND v.published_at < now()
         ORDER BY v.published_at DESC
         LIMIT 1
     ) AS v0
ORDER BY v0.published_at DESC
LIMIT 1;


SELECT v0.title, v0.permalink, v0.published_at
FROM (
         SELECT v.title, v.permalink, v.published_at
         FROM versioned_pages AS v
         WHERE v.published_at IS NOT NULL AND v.visibility = 1 AND v.type = 2 AND NOT (v.deleted) AND v.published_at > now()
         ORDER BY v.published_at
         LIMIT 1
     ) AS v0
ORDER BY v0.published_at
LIMIT 1;

--UPDATE dev_builds SET keep = true;
