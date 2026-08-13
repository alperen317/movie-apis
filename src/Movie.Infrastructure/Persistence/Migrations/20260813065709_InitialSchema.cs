using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Movie.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    avatar_variant = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "beam"),
                    avatar_seed = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    watch_region = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    email_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: true),
                    security_stamp = table.Column<string>(type: "text", nullable: true),
                    concurrency_stamp = table.Column<string>(type: "text", nullable: true),
                    phone_number = table.Column<string>(type: "text", nullable: true),
                    phone_number_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    two_factor_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    lockout_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lockout_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    access_failed_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "episode_progress",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    show_id = table.Column<int>(type: "integer", nullable: false),
                    season_number = table.Column<int>(type: "integer", nullable: false),
                    episode_number = table.Column<int>(type: "integer", nullable: false),
                    watched_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_episode_progress", x => new { x.user_id, x.show_id, x.season_number, x.episode_number });
                    table.ForeignKey(
                        name: "fk_episode_progress_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "lists",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    join_code = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lists", x => x.id);
                    table.CheckConstraint("lists_name_length", "char_length(btrim(name)) between 1 and 60");
                    table.ForeignKey(
                        name: "fk_lists_users_created_by_id",
                        column: x => x.created_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "recommendation_feedback",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    media_id = table.Column<int>(type: "integer", nullable: false),
                    media_type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recommendation_feedback", x => x.id);
                    table.ForeignKey(
                        name: "fk_recommendation_feedback_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "saved_media",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    list_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    media_id = table.Column<int>(type: "integer", nullable: false),
                    media_type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    poster_path = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    vote_average = table.Column<decimal>(type: "numeric", nullable: true),
                    year = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    genres = table.Column<string[]>(type: "text[]", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_saved_media", x => x.id);
                    table.ForeignKey(
                        name: "fk_saved_media_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_claims",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    claim_type = table.Column<string>(type: "text", nullable: true),
                    claim_value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_claims", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_claims_asp_net_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_logins",
                columns: table => new
                {
                    login_provider = table.Column<string>(type: "text", nullable: false),
                    provider_key = table.Column<string>(type: "text", nullable: false),
                    provider_display_name = table.Column<string>(type: "text", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_logins", x => new { x.login_provider, x.provider_key });
                    table.ForeignKey(
                        name: "fk_user_logins_asp_net_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_tokens",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    login_provider = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_tokens", x => new { x.user_id, x.login_provider, x.name });
                    table.ForeignKey(
                        name: "fk_user_tokens_asp_net_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "watch_log",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    media_id = table.Column<int>(type: "integer", nullable: false),
                    media_type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    poster_path = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    vote_average = table.Column<decimal>(type: "numeric", nullable: true),
                    year = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    genres = table.Column<string[]>(type: "text[]", nullable: false),
                    watched_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    rating = table.Column<short>(type: "smallint", nullable: true),
                    note = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_watch_log", x => x.id);
                    table.CheckConstraint("watch_log_rating_range", "rating is null or rating between 1 and 10");
                    table.ForeignKey(
                        name: "fk_watch_log_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "list_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    list_id = table.Column<Guid>(type: "uuid", nullable: false),
                    media_id = table.Column<int>(type: "integer", nullable: false),
                    media_type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    poster_path = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    vote_average = table.Column<decimal>(type: "numeric", nullable: true),
                    year = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    genres = table.Column<string[]>(type: "text[]", nullable: false),
                    added_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_list_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_list_items_lists_list_id",
                        column: x => x.list_id,
                        principalTable: "lists",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_list_items_users_added_by_id",
                        column: x => x.added_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "list_members",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    list_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false, defaultValue: "member"),
                    status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false, defaultValue: "pending"),
                    invited_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    responded_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_list_members", x => x.id);
                    table.ForeignKey(
                        name: "fk_list_members_lists_list_id",
                        column: x => x.list_id,
                        principalTable: "lists",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_list_members_users_invited_by_id",
                        column: x => x.invited_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_list_members_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "list_polls",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    list_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    deadline = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_list_polls", x => x.id);
                    table.ForeignKey(
                        name: "fk_list_polls_lists_list_id",
                        column: x => x.list_id,
                        principalTable: "lists",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_list_polls_users_created_by_id",
                        column: x => x.created_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "list_poll_candidates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    poll_id = table.Column<Guid>(type: "uuid", nullable: false),
                    list_item_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_list_poll_candidates", x => x.id);
                    table.ForeignKey(
                        name: "fk_list_poll_candidates_list_items_list_item_id",
                        column: x => x.list_item_id,
                        principalTable: "list_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_list_poll_candidates_list_polls_poll_id",
                        column: x => x.poll_id,
                        principalTable: "list_polls",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "list_poll_votes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    poll_id = table.Column<Guid>(type: "uuid", nullable: false),
                    candidate_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_list_poll_votes", x => x.id);
                    table.ForeignKey(
                        name: "fk_list_poll_votes_list_poll_candidates_candidate_id",
                        column: x => x.candidate_id,
                        principalTable: "list_poll_candidates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_list_poll_votes_list_polls_poll_id",
                        column: x => x.poll_id,
                        principalTable: "list_polls",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_list_poll_votes_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "episode_progress_user_show_idx",
                table: "episode_progress",
                columns: new[] { "user_id", "show_id" });

            migrationBuilder.CreateIndex(
                name: "ix_list_items_added_by_id",
                table: "list_items",
                column: "added_by");

            migrationBuilder.CreateIndex(
                name: "list_items_list_idx",
                table: "list_items",
                columns: new[] { "list_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "list_items_list_media_key",
                table: "list_items",
                columns: new[] { "list_id", "media_id", "media_type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_list_members_invited_by_id",
                table: "list_members",
                column: "invited_by");

            migrationBuilder.CreateIndex(
                name: "list_members_list_idx",
                table: "list_members",
                columns: new[] { "list_id", "status" });

            migrationBuilder.CreateIndex(
                name: "list_members_list_user_key",
                table: "list_members",
                columns: new[] { "list_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "list_members_user_idx",
                table: "list_members",
                columns: new[] { "user_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_list_poll_candidates_list_item_id",
                table: "list_poll_candidates",
                column: "list_item_id");

            migrationBuilder.CreateIndex(
                name: "list_poll_candidates_poll_item_key",
                table: "list_poll_candidates",
                columns: new[] { "poll_id", "list_item_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_list_poll_votes_candidate_id",
                table: "list_poll_votes",
                column: "candidate_id");

            migrationBuilder.CreateIndex(
                name: "ix_list_poll_votes_user_id",
                table: "list_poll_votes",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "list_poll_votes_poll_user_key",
                table: "list_poll_votes",
                columns: new[] { "poll_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_list_polls_created_by_id",
                table: "list_polls",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "list_polls_list_idx",
                table: "list_polls",
                columns: new[] { "list_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "lists_created_by_idx",
                table: "lists",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "lists_join_code_key",
                table: "lists",
                column: "join_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "recommendation_feedback_user_media_key",
                table: "recommendation_feedback",
                columns: new[] { "user_id", "media_type", "media_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "saved_media_user_list_idx",
                table: "saved_media",
                columns: new[] { "user_id", "list_type", "created_at" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "saved_media_user_list_media_key",
                table: "saved_media",
                columns: new[] { "user_id", "list_type", "media_id", "media_type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_claims_user_id",
                table: "user_claims",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_logins_user_id",
                table: "user_logins",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "users_email_idx",
                table: "users",
                column: "normalized_email");

            migrationBuilder.CreateIndex(
                name: "users_user_name_key",
                table: "users",
                column: "normalized_user_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "watch_log_user_media_idx",
                table: "watch_log",
                columns: new[] { "user_id", "media_type", "media_id" });

            migrationBuilder.CreateIndex(
                name: "watch_log_user_watched_idx",
                table: "watch_log",
                columns: new[] { "user_id", "watched_at" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "episode_progress");

            migrationBuilder.DropTable(
                name: "list_members");

            migrationBuilder.DropTable(
                name: "list_poll_votes");

            migrationBuilder.DropTable(
                name: "recommendation_feedback");

            migrationBuilder.DropTable(
                name: "saved_media");

            migrationBuilder.DropTable(
                name: "user_claims");

            migrationBuilder.DropTable(
                name: "user_logins");

            migrationBuilder.DropTable(
                name: "user_tokens");

            migrationBuilder.DropTable(
                name: "watch_log");

            migrationBuilder.DropTable(
                name: "list_poll_candidates");

            migrationBuilder.DropTable(
                name: "list_items");

            migrationBuilder.DropTable(
                name: "list_polls");

            migrationBuilder.DropTable(
                name: "lists");

            migrationBuilder.DropTable(
                name: "users");
        }
    }
}
