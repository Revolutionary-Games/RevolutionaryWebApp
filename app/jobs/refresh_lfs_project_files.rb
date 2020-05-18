# frozen_string_literal: true

# Just refreshes an Lfs project files (if the commit changes)
class RefreshLfsProjectFiles < ApplicationJob
  queue_as :default

  def perform(id)
    project = LfsProject.find_by_id id

    unless project
      logger.warning "Can't refresh files for non-existant lfs project"
      return
    end

    GitFilesHelper.update_files project
  end
end
