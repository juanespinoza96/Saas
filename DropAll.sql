-- ============================================================
-- SCRIPT: Eliminar TODAS las tablas, secuencias, tipos y 
-- extensiones del schema public (dejar la DB completamente limpia)
-- ============================================================

-- Desactivar verificación de foreign keys durante el drop
SET session_replication_role = 'replica';

-- Eliminar todas las tablas del schema public
DO $$
DECLARE
    r RECORD;
BEGIN
    FOR r IN (SELECT tablename FROM pg_tables WHERE schemaname = 'public') LOOP
        EXECUTE 'DROP TABLE IF EXISTS "' || r.tablename || '" CASCADE';
    END LOOP;
END $$;

-- Eliminar todas las secuencias
DO $$
DECLARE
    r RECORD;
BEGIN
    FOR r IN (SELECT sequencename FROM pg_sequences WHERE schemaname = 'public') LOOP
        EXECUTE 'DROP SEQUENCE IF EXISTS "' || r.sequencename || '" CASCADE';
    END LOOP;
END $$;

-- Eliminar todos los tipos custom (enums, composites)
DO $$
DECLARE
    r RECORD;
BEGIN
    FOR r IN (
        SELECT t.typname
        FROM pg_type t
        JOIN pg_namespace n ON t.typnamespace = n.oid
        WHERE n.nspname = 'public'
          AND t.typtype IN ('e', 'c')  -- enums y composites
          AND t.typname NOT LIKE '%[]'
    ) LOOP
        EXECUTE 'DROP TYPE IF EXISTS "' || r.typname || '" CASCADE';
    END LOOP;
END $$;

-- Restaurar verificación de foreign keys
SET session_replication_role = 'origin';

-- Verificar que no queda nada
SELECT tablename FROM pg_tables WHERE schemaname = 'public';
