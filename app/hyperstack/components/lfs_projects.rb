# frozen_string_literal: true

# Table row
class LFSProjectItem < HyperComponent
  include Hyperstack::Router::Helpers

  param :project
  render(TR) do
    TH(scope: 'row') { Link("/lfs/#{@Project.slug}") { @Project.name } }
    # TD { 'Slug' }
    TD { @Project.public?.to_s }
    TD { @Project.total_size_mib.to_s + ' MiB' }
    TD { @Project.updated_at.to_s }
  end
end

# Lists all LFS projects in a table
class LFSProjects < HyperComponent
  # To quiet some react warnings
  include Hyperstack::Router::Helpers

  param :current_page, default: 0, type: Integer

  def items
    LfsProject.visible_to(Hyperstack::Application.acting_user_id).order_by_name
  end

  render(DIV) do
    H1 { 'Git LFS Projects' }

    Paginator(current_page: @CurrentPage,
              item_count: items.count,
              ref: set(:paginator)) {
      # This is set with a delay
      if @paginator
        RS.Table(:striped) {
          THEAD {
            TR {
              TH { 'Name' }
              # TH { 'Slug' }
              TH { 'Public' }
              TH { 'Size' }
              TH { 'Last modified' }
            }
          }

          TBODY {
            items.each { |project|
              LFSProjectItem(project: project)
            }
          }
        }
      end
    }.on(:page_changed) { |page|
      mutate @CurrentPage = page
    }
  end
end
