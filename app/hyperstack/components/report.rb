class ReportView < HyperComponent
  include Hyperstack::Router::Helpers

  render(DIV) do

    report = Report.find_by_id match.params[:id]

    H1 { "Report #{report.id}" }

    # H1 { "You are not allowed to view this report or it doesn't exist"} if !report.????

    UL {
      LI { "Game Version: #{report.game_version}" }
      LI { "Public: " + (report.public ? "yes" : "no")}
      LI { "Updated At: #{report.updated_at}" }
      LI { "Created At: #{report.created_at}" }
      LI { "Crash Time: #{report.crash_time}" }
      LI { "Solved: " + (report.solved ? "yes" : "no")}
      LI { "Solve comment: #{report.solved_comment}" }
      LI { "Reporter IP: #{report.reporter_ip}" } if App.acting_user&.developer?
      LI { "Reporter Email: #{report.reporter_email}" } if App.acting_user&.admin?
      LI { "Duplicate of: #{report.duplicate_of_id}" }
    }

    H2 { "Description" }
    P { report.description }
    P { report.extra_description }

    H2 { "Notes" }
    P { PRE{ report.notes } }

    H2 { "Callstack" }
    P { PRE{ report.primary_callstack } }

    H2 { "Log files" }
    P { PRE{ report.log_files } }

    H2 { "Full dump" }
    P { PRE{ report.processed_dump } }



  end
end
