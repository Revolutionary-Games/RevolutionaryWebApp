# frozen_string_literal: true

# Fully recreates the list of files for a lfs project
class RebuildLfsProjectFilesJob < ApplicationJob
  queue_as :default

  def perform(id)
    project = LfsProject.find_by_id id

    unless project
      logger.warn "Can't rebuild files for non-existant lfs project"
      return
    end

    GitFilesHelper.delete_all_file_objects project
    GitFilesHelper.update_files project
  end
end
