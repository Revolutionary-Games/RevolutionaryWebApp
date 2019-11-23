# frozen_string_literal: true

# Single table line
class ReportItem < HyperComponent
  include Hyperstack::Router::Helpers

  param :report
  render(TR) do
    TD { @Report.game_version.to_s }
    TH(scope: 'row') { @Report.id.to_s }
    TD { Link("/report/#{@Report.id}") { @Report.updated_at.to_s } }
    TD { @Report.solved ? 'yes' : 'no' }
    # TD{"#{@Report.crash_time}"}
    TD { @Report.description.to_s }
    TD { @Report.public.to_s }
    TD { @Report.solved_comment.to_s }
  end
end

# A table of crash reports
class Reports < HyperComponent
  param :current_page, default: 0, type: Integer
  param :page_size, default: 25, type: Integer

  def items
    Report.visible_to(Hyperstack::Application.acting_user_id).index_by_updated_at
  end

  render(DIV) do
    H1 { 'Crash reports' }

    Paginator(current_page: @CurrentPage,
              page_size: @PageSize,
              item_count: items.count,
              on_page_changed: lambda { |page|
                mutate @CurrentPage = page
              })

    ReactStrap.Table(:striped, :responsive) {
      THEAD {
        TR {
          TH { 'Version' }
          TH { 'ID' }
          TH { 'Updated At' }
          TH { 'Solved' }
          # TH{ "Crash Time" }
          TH { 'Description' }
          TH { 'Public' }
          TH { 'Solve comment' }
        }
      }

      TBODY {
        items.offset(@CurrentPage * @PageSize).take(@PageSize).each { |report|
          ReportItem(report: report)
        }
      }
    }
  end
end
