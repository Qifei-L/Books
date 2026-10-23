using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Books.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLastAdminGuard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Rule 8, final guard: at commit, an active organization must still have an active administrator.
            // The organization row is locked before counting, so concurrent changes are checked one after another.
            migrationBuilder.Sql("""
                CREATE FUNCTION ensure_org_has_admin() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE
                    target uuid;
                    org_state text;
                BEGIN
                    IF TG_TABLE_NAME = 'organizations' THEN
                        target := NEW.id;
                    ELSIF TG_OP = 'DELETE' THEN
                        target := OLD.org_id;
                    ELSE
                        target := NEW.org_id;
                    END IF;
                    SELECT state INTO org_state FROM organizations WHERE id = target FOR UPDATE;
                    IF org_state = 'active' AND NOT EXISTS (
                        SELECT 1 FROM memberships WHERE org_id = target AND role = 'admin' AND status = 'active'
                    ) THEN
                        RAISE EXCEPTION 'organization % would have no active administrator', target
                            USING ERRCODE = 'integrity_constraint_violation', CONSTRAINT = 'org_has_admin';
                    END IF;
                    RETURN NULL;
                END $$;
                CREATE CONSTRAINT TRIGGER memberships_keep_admin AFTER INSERT OR UPDATE OR DELETE ON memberships
                    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION ensure_org_has_admin();
                CREATE CONSTRAINT TRIGGER organizations_need_admin AFTER INSERT OR UPDATE OF state ON organizations
                    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION ensure_org_has_admin();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER organizations_need_admin ON organizations;
                DROP TRIGGER memberships_keep_admin ON memberships;
                DROP FUNCTION ensure_org_has_admin();
                """);
        }
    }
}
