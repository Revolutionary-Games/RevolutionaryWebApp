# frozen_string_literal: true

# Table row
class LFSProjectItem < HyperComponent
  include Hyperstack::Router::Helpers

  param :project
  render(TR) do
    TD { Link("/lfs/#{@Project.slug}") { @Project.name } }
    # TD { 'Slug' }
    TD { @Project.public?.to_s }
    TD { @Project.updated_at.to_s }
  end
end

# Lists all LFS projects in a table
class LFSProjects < HyperComponent
  # To quiet some react warnings
  include Hyperstack::Router::Helpers

  render(DIV) do
    H1 { 'Git LFS Projects' }

    TABLE {
      THEAD {
        TR {
          TD { 'Name' }
          # TD { 'Slug' }
          TD { 'Public' }
          TD { 'Last modified' }
        }
      }

      TBODY {
        LfsProject.visible_to(Hyperstack::Application.acting_user_id)
                  .order_by_name.each { |project|
          LFSProjectItem(project: project)
        }
      }
    }
  end
end
