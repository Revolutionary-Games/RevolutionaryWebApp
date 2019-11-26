# frozen_string_literal: true

# Load the Rails application.
require_relative 'application'

# Doesn't seem to work
# # Fix multiple workers with hyperstack
# MiniRacer::Platform.set_flags! :noconcurrent_recompilation, :noconcurrent_sweeping

# Initialize the Rails application.
Rails.application.initialize!
