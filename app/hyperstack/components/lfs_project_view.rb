# frozen_string_literal: true

# Shows a single LFS project
class LfsProjectView < HyperComponent
  include Hyperstack::Router::Helpers

  render(DIV) do
    project = LfsProject.find_by slug: match.params[:slug]

    unless project
      H1 { 'No project found' }
      return
    end

    H1 { "Git LFS Project #{project.name}" }

    UL {
      LI { 'Public: ' + (project.public ? 'yes' : 'no') }
      LI { "Updated At: #{project.updated_at}" }
      LI { "Created At: #{project.created_at}" }
    }

    H2 { 'Statistics' }
    P { 'TODO: total size and item count' }

    H2 { 'Files' }
    P { 'TODO: some kind of list' }
  end
end
