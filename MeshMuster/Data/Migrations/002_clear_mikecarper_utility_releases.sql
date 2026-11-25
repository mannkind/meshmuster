-- mikecarper's utility-v tags were filed as sensor firmware. Their 1.18+ releases are stale, so
-- drop them and clear the source's ETag so the next poll re-reads the release list. Only that
-- source and its sensor stream are touched; devices keep their version label and flash history.

UPDATE sources
   SET etag = ''
 WHERE slug = 'meshcore-mikecarper' AND kind = 'firmware';

DELETE FROM releases
 WHERE tag LIKE 'utility-v%'
   AND sort_key >= '00001.00018.00000.00000'
   AND stream_id IN (
       SELECT st.id
         FROM streams st
         JOIN sources so ON so.id = st.source_id
        WHERE so.slug = 'meshcore-mikecarper' AND st.slug = 'sensor');
